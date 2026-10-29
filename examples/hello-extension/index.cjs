// Contract fixture, not evidence that third-party Deckboard extensions all work.
module.exports = {
  name: 'Hello MacDock',
  inputs: [{ label: '테스트 메시지', value: 'hello' }],
  execute(action, params) {
    if (action !== 'hello') throw new Error('Unknown action');
    console.log('Hello MacDock:', params.message || 'Standalone extension host works.');
  }
};
